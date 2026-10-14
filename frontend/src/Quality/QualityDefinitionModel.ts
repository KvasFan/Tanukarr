import ModelBase from 'App/ModelBase';
import Quality, { QualitySource } from './Quality';

export default interface QualityDefinitionModel extends ModelBase {
  quality: Quality;
  title: string;
  weight: number;
  minSize: number | null;
  maxSize: number | null;
  preferredSize: number | null;
  minSizeIsMixed?: boolean;
  maxSizeIsMixed?: boolean;
  preferredSizeIsMixed?: boolean;
  source?: QualitySource;
  resolution?: number;
  codec?: string;
  titlePattern?: string;
  isCustom?: boolean;
  isRetired?: boolean;
}
