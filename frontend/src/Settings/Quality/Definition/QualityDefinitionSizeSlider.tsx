import React, {
  HTMLProps,
  useCallback,
  useEffect,
  useRef,
  useState,
} from 'react';
import ReactSlider from 'react-slider';
import roundNumber from 'Utilities/Number/roundNumber';
import translate from 'Utilities/String/translate';
import styles from './QualityDefinition.module.css';

const MIN = 0;
const MAX = 1000;
const STEP_SIZE = 0.1;
const MIN_DISTANCE = 3;
const SLIDER_MAX = roundNumber(Math.pow(MAX, 1 / 1.1));
const SLIDER_PREFERRED_DEFAULT = SLIDER_MAX - MIN_DISTANCE;

type SizeField = 'minSize' | 'preferredSize' | 'maxSize';
type SliderValues = [number, number, number];

const FIELDS: SizeField[] = ['minSize', 'preferredSize', 'maxSize'];

interface QualityDefinitionSizeSliderProps {
  qualityName: string;
  minSize: number | null;
  preferredSize: number | null;
  maxSize: number | null;
  // Values are MiB per minute, matching the stored definition fields.
  onChange(field: SizeField, value: number | null): void;
}

function trackRenderer(props: HTMLProps<HTMLDivElement>) {
  return <div {...props} className={styles.track} />;
}

function thumbRenderer(props: HTMLProps<HTMLDivElement>) {
  return <div {...props} className={styles.thumb} />;
}

function getSliderValue(value: number | null, defaultValue: number) {
  return roundNumber(value == null ? defaultValue : Math.pow(value, 1 / 1.1));
}

function getSliderValues(
  minSize: number | null,
  preferredSize: number | null,
  maxSize: number | null
): SliderValues {
  return [
    getSliderValue(minSize, MIN),
    getSliderValue(preferredSize, SLIDER_PREFERRED_DEFAULT),
    getSliderValue(maxSize === 0 ? null : maxSize, SLIDER_MAX),
  ];
}

function getSize(field: SizeField, sliderValue: number) {
  if (
    (field === 'preferredSize' && sliderValue === SLIDER_PREFERRED_DEFAULT) ||
    (field === 'maxSize' && sliderValue === SLIDER_MAX)
  ) {
    return null;
  }

  return roundNumber(Math.pow(sliderValue, 1.1));
}

function QualityDefinitionSizeSlider({
  qualityName,
  minSize,
  preferredSize,
  maxSize,
  onChange,
}: QualityDefinitionSizeSliderProps) {
  const [values, setValues] = useState<SliderValues>(() =>
    getSliderValues(minSize, preferredSize, maxSize)
  );
  const current = useRef(values);
  const isDragging = useRef(false);

  const update = useCallback((updated: SliderValues) => {
    current.current = updated;
    setValues(updated);
  }, []);

  useEffect(() => {
    // Typed values move the thumbs; a drag in progress keeps its own position.
    if (!isDragging.current) {
      update(getSliderValues(minSize, preferredSize, maxSize));
    }
  }, [minSize, preferredSize, maxSize, update]);

  const handleChange = useCallback(
    (updated: SliderValues) => {
      isDragging.current = true;

      // Emit only the thumbs that moved, so a limit that varies between
      // profiles is not overwritten by dragging a different thumb.
      FIELDS.forEach((field, index) => {
        if (updated[index] !== current.current[index]) {
          onChange(field, getSize(field, updated[index]));
        }
      });

      update(updated);
    },
    [onChange, update]
  );

  const handleAfterChange = useCallback(() => {
    isDragging.current = false;
    update(getSliderValues(minSize, preferredSize, maxSize));
  }, [minSize, preferredSize, maxSize, update]);

  return (
    <div className={styles.sizeSlider}>
      {/* eslint-disable-next-line @typescript-eslint/ban-ts-comment */}
      {/* @ts-ignore React version mismatch */}
      <ReactSlider
        className={styles.slider}
        min={MIN}
        max={SLIDER_MAX}
        step={STEP_SIZE}
        minDistance={MIN_DISTANCE}
        value={values}
        withTracks={true}
        // eslint-disable-next-line @typescript-eslint/ban-ts-comment
        // @ts-ignore allowCross is still available in the version currently used
        allowCross={false}
        snapDragDisabled={true}
        pearling={true}
        ariaLabel={[
          `${qualityName}: ${translate('Minimum')}`,
          `${qualityName}: ${translate('Preferred')}`,
          `${qualityName}: ${translate('Maximum')}`,
        ]}
        renderThumb={thumbRenderer}
        renderTrack={trackRenderer}
        onChange={handleChange}
        onAfterChange={handleAfterChange}
      />
    </div>
  );
}

export default QualityDefinitionSizeSlider;
