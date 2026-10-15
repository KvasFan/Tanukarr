import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import LazyLoadExport from 'react-lazyload';
import translate from 'Utilities/String/translate';
import { CoverType, Image } from './Series';

function getLazyLoadComponent(value: unknown): typeof LazyLoadExport {
  if (typeof value === 'function') {
    return value as typeof LazyLoadExport;
  }

  // Rolldown's Node interop exposes this CommonJS package as a namespace.
  if (
    value &&
    typeof value === 'object' &&
    'default' in value &&
    typeof value.default === 'function'
  ) {
    return value.default as typeof LazyLoadExport;
  }

  throw new TypeError('react-lazyload did not export a component');
}

const LazyLoad = getLazyLoadComponent(LazyLoadExport);

function findImage(images: Image[], coverType: CoverType) {
  return images.find(
    (image) => image.coverType === coverType && (image.url || image.remoteUrl)
  );
}

function getUrl(image: Image, coverType: CoverType, size: number) {
  const imageUrl = image?.url || image?.remoteUrl;

  if (!imageUrl) {
    return null;
  }

  if (!imageUrl.startsWith(`${window.Sonarr?.urlBase ?? ''}/MediaCover/`)) {
    return imageUrl;
  }

  const heights: Partial<Record<CoverType, number[]>> = {
    poster: [250, 500],
    banner: [35, 70],
    fanart: [180, 360],
  };
  const available = heights[coverType];

  if (!available) {
    return imageUrl;
  }

  const height =
    available.find((value) => value >= size) ?? available[available.length - 1];
  return imageUrl.replace(
    new RegExp(`${coverType}\\.jpg(?=$|[?#])`),
    `${coverType}-${height}.jpg`
  );
}

export interface SeriesImageProps {
  className?: string;
  style?: object;
  images: Image[];
  coverType: CoverType;
  placeholder: string;
  size?: number;
  lazy?: boolean;
  overflow?: boolean;
  title: string;
  onError?: () => void;
  onLoad?: () => void;
}

const pixelRatio = Math.max(Math.round(window.devicePixelRatio), 1);

function SeriesImage({
  className,
  style,
  images,
  coverType,
  placeholder,
  size = 250,
  lazy = true,
  overflow = false,
  title,
  onError,
  onLoad,
}: SeriesImageProps) {
  const [url, setUrl] = useState<string | null>(null);
  const [hasError, setHasError] = useState(false);
  const [isLoaded, setIsLoaded] = useState(true);
  const image = useRef<Image | null>(null);
  const attemptedUrls = useRef(new Set<string>());

  const alt = useMemo(() => {
    let type = translate('ImagePoster');

    switch (coverType) {
      case 'banner':
        type = translate('ImageBanner');
        break;
      case 'fanart':
        type = translate('ImageFanart');
        break;
      case 'season':
        type = translate('ImageSeason');
        break;
      default:
        break;
    }

    return `${title} ${type}`;
  }, [title, coverType]);

  const handleLoad = useCallback(() => {
    setHasError(false);
    setIsLoaded(true);
    onLoad?.();
  }, [setHasError, setIsLoaded, onLoad]);

  const handleError = useCallback(() => {
    if (url) {
      attemptedUrls.current.add(url);
    }

    const nextUrl = [image.current?.url, image.current?.remoteUrl].find(
      (candidate) =>
        candidate &&
        !attemptedUrls.current.has(candidate) &&
        (candidate === image.current?.url || /^https:\/\//i.test(candidate))
    );

    if (nextUrl) {
      setUrl(nextUrl);
      setHasError(false);
      setIsLoaded(true);
      return;
    }

    setHasError(true);
    setIsLoaded(false);
    onError?.();
  }, [url, onError]);

  useEffect(() => {
    const nextImage = findImage(images, coverType);

    if (
      nextImage &&
      (!image.current ||
        nextImage.url !== image.current.url ||
        nextImage.remoteUrl !== image.current.remoteUrl)
    ) {
      // Don't reset isLoaded, as we want to immediately try to
      // show the new image, whether an image was shown previously
      // or the placeholder was shown.
      image.current = nextImage;
      attemptedUrls.current.clear();

      setUrl(getUrl(nextImage, coverType, pixelRatio * size));
      setHasError(false);
      setIsLoaded(true);
    } else if (!nextImage) {
      if (image.current) {
        image.current = null;
        setUrl(placeholder);
        setHasError(false);
        onError?.();
      }
    }
  }, [images, coverType, placeholder, size, onError]);

  useEffect(() => {
    if (!image.current) {
      onError?.();
    }
    // This should only run once when the component mounts,
    // so we don't need to include the other dependencies.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (hasError || !url) {
    return (
      <img alt={alt} className={className} style={style} src={placeholder} />
    );
  }

  if (lazy) {
    return (
      <LazyLoad
        height={size}
        offset={100}
        overflow={overflow}
        placeholder={
          <img
            alt={alt}
            className={className}
            style={style}
            src={placeholder}
          />
        }
      >
        <img
          alt={alt}
          className={className}
          style={style}
          src={url}
          rel="noreferrer"
          referrerPolicy="no-referrer"
          onError={handleError}
          onLoad={handleLoad}
        />
      </LazyLoad>
    );
  }

  return (
    <img
      alt={alt}
      className={className}
      style={style}
      src={isLoaded ? url : placeholder}
      referrerPolicy="no-referrer"
      onError={handleError}
      onLoad={handleLoad}
    />
  );
}

export default SeriesImage;
